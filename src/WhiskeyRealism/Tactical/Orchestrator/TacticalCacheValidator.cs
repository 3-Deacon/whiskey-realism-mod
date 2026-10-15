using System;
using System.Collections;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Stage D validation harness (2026-05-21). Periodically samples cached structural
    // data and compares it to live Unity state. If any cache becomes stale (an
    // invalidation event was missed, a Unity object changed unexpectedly, a roster
    // mutation hook didn't fire) we detect the divergence and emit a telemetry
    // event so smoke can spot silent correctness failures introduced by the caches.
    //
    // What we validate:
    //   - TacticalStableSceneCache.MapObjectives count matches the current scene scan
    //     (Objectives don't appear or disappear mid-battle; a count change means
    //     either vanilla added objectives at runtime or our cache forgot to refresh).
    //   - TacticalStableSceneCache.ApproachAvenues count matches a fresh scan (entry
    //     points are stable; mismatch indicates the same class of bug).
    //   - CommandTreeRuntime cached snapshot has non-empty Nodes when there are
    //     active regiments for that alliance (catches the case where the cache
    //     was populated empty by accident).
    //   - _resolvedDecisionsBySide has at least one entry per side with active
    //     commandable regiments (catches the case where pre-populate failed silently).
    //
    // Cadence: ValidateIfDue() is called from Tick() body. Internally rate-limited
    // to once per ValidationIntervalSeconds (default 30s wallclock) so the validation
    // walk itself doesn't add per-tick cost.
    //
    // Schema: emits TacticalCacheValidation events tagged in tactical.jsonl. Always
    // emits a summary event each validation (count of caches validated, mismatches
    // found). Emits TacticalCacheValidationFailure as Warning on mismatch.
    internal static class TacticalCacheValidator
    {
        private const float ValidationIntervalSeconds = 30f;
        private static float _lastValidationRealtimeSeconds;
        private static long _validationsRun;
        private static long _mismatchesFound;

        // Called from Tick() body. Cheap if not due (single comparison + early-return).
        public static void ValidateIfDue()
        {
            try
            {
                float now = SafeRealtimeSeconds();
                if (now - _lastValidationRealtimeSeconds < ValidationIntervalSeconds) return;
                _lastValidationRealtimeSeconds = now;

                using (TelemetryPerf.Scope(
                    "tactical.cache-validator.run",
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Performance,
                    5.0))
                {
                    int mismatches = 0;
                    mismatches += ValidateSceneCache();
                    mismatches += ValidateCommandTreeCache();
                    mismatches += ValidateResolvedDecisionsCache();
                    _validationsRun++;
                    _mismatchesFound += mismatches;

                    TelemetryRouter.Emit(
                        TelemetryLayer.Tactical,
                        TelemetryCategory.Health,
                        "TacticalCacheValidation",
                        mismatches > 0 ? TelemetrySeverity.Warning : TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision(mismatches > 0 ? "mismatch" : "clean", "periodic", "validator")
                            .WithField("validationsRun", _validationsRun)
                            .WithField("mismatchesFound", _mismatchesFound)
                            .WithField("thisRunMismatches", mismatches));
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning(
                    "tactical-cache-validator:exception",
                    "[TacticalCacheValidator] validator threw: " + e.GetType().Name + " " + e.Message);
            }
        }

        public static void ResetForBattleEnd()
        {
            _lastValidationRealtimeSeconds = 0f;
            _validationsRun = 0;
            _mismatchesFound = 0;
        }

        // Compares cached map-objective + approach-avenue counts to a fresh scene scan.
        // Mismatch on count means the cache has gone stale and a live-scan rebuild is
        // needed. Returns mismatch count (0 = clean).
        private static int ValidateSceneCache()
        {
            int mismatches = 0;
            try
            {
                if (!TacticalStableSceneCache.IsPopulated) return 0;

                var cachedObjectives = TacticalStableSceneCache.GetOrCaptureMapObjectives();
                var liveObjectives = TacticalVisionRuntimeAdapter.CaptureMapObjectivesLive();
                if (cachedObjectives.Length != liveObjectives.Length)
                {
                    mismatches++;
                    OnceLog.Warning(
                        "tactical-cache-validator:scene-map-objectives-drift",
                        "[TacticalCacheValidator] map objectives drift: cached="
                        + cachedObjectives.Length + " live=" + liveObjectives.Length
                        + " — scene scan returned different count; cache may be stale");
                }

                var cachedAvenues = TacticalStableSceneCache.GetOrCaptureApproachAvenues(0);
                var liveAvenues = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsLive(0);
                if (cachedAvenues.Length != liveAvenues.Length)
                {
                    mismatches++;
                    OnceLog.Warning(
                        "tactical-cache-validator:scene-avenues-drift",
                        "[TacticalCacheValidator] approach avenues drift: cached="
                        + cachedAvenues.Length + " live=" + liveAvenues.Length
                        + " — entry points changed during battle (unexpected)");
                }
            }
            catch { /* validator must never throw out of Tick */ }
            return mismatches;
        }

        // Checks that command-tree cache has non-empty Nodes when there are active
        // commandable regiments for that alliance. Empty cache for a populated side
        // signals a stale cache that didn't pick up roster.
        private static int ValidateCommandTreeCache()
        {
            int mismatches = 0;
            try
            {
                for (int allianceId = 0; allianceId < 2; allianceId++)
                {
                    long hits = CommandTreeRuntime.GetCacheHits(allianceId);
                    long misses = CommandTreeRuntime.GetCacheMisses(allianceId);
                    if (hits + misses == 0) continue; // never called for this alliance

                    var snap = CommandTreeRuntime.Snapshot(allianceId);
                    bool hasAnyRegiments = CountActiveRegimentsForAlliance(allianceId) > 0;
                    if (hasAnyRegiments && (snap == null || snap == CommandTreeSnapshot.Empty))
                    {
                        mismatches++;
                        OnceLog.Warning(
                            "tactical-cache-validator:command-tree-empty-but-populated:" + allianceId,
                            "[TacticalCacheValidator] command tree cache empty for alliance="
                            + allianceId + " but live roster has active regiments — invalidating");
                        CommandTreeRuntime.Invalidate(allianceId);
                    }
                }
            }
            catch { }
            return mismatches;
        }

        // Walks completeunitlist and counts non-routed regiments matching the alliance.
        // Cheap (single linear scan, no allocations). Used by ValidateCommandTreeCache.
        private static int CountActiveRegimentsForAlliance(int allianceId)
        {
            try
            {
                var units = BattleUnits.completeunitlist as IList;
                if (units == null || units.Count == 0) return 0;
                int count = 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null) continue;
                    if (reg.alliance != allianceId) continue;
                    if (reg.isrouted) continue;
                    if (reg.markedforrout) continue;
                    if (reg.permanentlydetached) continue;
                    count++;
                }
                return count;
            }
            catch { return 0; }
        }

        // Validates that _resolvedDecisionsBySide has at least one entry per side
        // that has active commandable regiments. If it's empty, the patch falls
        // through to tier-3 ApplyGroup for every group — exactly the spike pattern
        // Stage C was designed to eliminate. Detection means pre-populate or
        // refresh missed; emit warning so smoke catches the regression.
        private static int ValidateResolvedDecisionsCache()
        {
            int mismatches = 0;
            try
            {
                for (int allianceId = 0; allianceId < 2; allianceId++)
                {
                    int activeRegiments = CountActiveRegimentsForAlliance(allianceId);
                    if (activeRegiments == 0) continue;

                    // Probe by looking up a known regiment via TryGetResolvedGroupDecision.
                    // We don't have direct access to the dict size, so we probe the FIRST
                    // active regiment's instanceId. Miss means the cache is empty for
                    // this side (a fault either in pre-populate or in heavy-gate refresh).
                    int probedInstanceId = ProbeFirstActiveRegimentInstanceId(allianceId);
                    if (probedInstanceId == 0) continue;

                    TacticalResolvedGroupDecision dummy;
                    bool hit = TacticalBattleCoordinator.TryGetResolvedGroupDecision(
                        allianceId, probedInstanceId, out dummy);
                    if (!hit)
                    {
                        mismatches++;
                        OnceLog.Warning(
                            "tactical-cache-validator:resolved-cache-miss:" + allianceId,
                            "[TacticalCacheValidator] resolved-decisions cache MISS for alliance="
                            + allianceId + " probedInstanceId=" + probedInstanceId
                            + " activeRegiments=" + activeRegiments
                            + " — tier-3 ApplyGroup fallback active; perf regression likely");
                    }
                }
            }
            catch { }
            return mismatches;
        }

        private static int ProbeFirstActiveRegimentInstanceId(int allianceId)
        {
            try
            {
                var units = BattleUnits.completeunitlist as IList;
                if (units == null) return 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null) continue;
                    if (reg.alliance != allianceId) continue;
                    if (reg.isrouted || reg.markedforrout || reg.permanentlydetached) continue;
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(reg.unittyp)) continue;
                    try { return reg.GetInstanceID(); } catch { continue; }
                }
            }
            catch { }
            return 0;
        }

        private static float SafeRealtimeSeconds()
        {
            try { return Time.realtimeSinceStartup; }
            catch { return 0f; }
        }
    }
}
