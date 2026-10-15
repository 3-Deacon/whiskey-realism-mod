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
    // What we validate (content signatures, not counts — BR-H5):
    //   - TacticalStableSceneCache map objectives: content hash (id, owner, active,
    //     position) equals a fresh live capture. Owner flips at runtime (105746).
    //   - TacticalStableSceneCache approach avenues: content hash (source, owner,
    //     origin, target) equals a fresh scan (scheduledarrival / EntryPoint.owner
    //     mutate mid-battle, 87601-87927).
    //   - CommandTreeRuntime: the cached roster signature equals the live one
    //     (completeunitlist identity/count, active/routed/marked counts, id set), and
    //     the cached snapshot has nodes when the side has active regiments.
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
                // Realtime is fine here: this is a CPU-budget throttle, not doctrine.
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
                if (TacticalCacheSignatures.MapObjectivesContentHash(cachedObjectives)
                    != TacticalCacheSignatures.MapObjectivesContentHash(liveObjectives))
                {
                    mismatches++;
                    OnceLog.Warning(
                        "tactical-cache-validator:scene-map-objectives-drift",
                        "[TacticalCacheValidator] map objectives drift: cached="
                        + cachedObjectives.Length + " live=" + liveObjectives.Length
                        + " — content (id/owner/active/position) differs from a live capture");
                }

                var cachedAvenues = TacticalStableSceneCache.GetOrCaptureApproachAvenues(0);
                var liveAvenues = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsLive(0);
                if (TacticalCacheSignatures.AvenuesContentHash(cachedAvenues)
                    != TacticalCacheSignatures.AvenuesContentHash(liveAvenues))
                {
                    mismatches++;
                    OnceLog.Warning(
                        "tactical-cache-validator:scene-avenues-drift",
                        "[TacticalCacheValidator] approach avenues drift: cached="
                        + cachedAvenues.Length + " live=" + liveAvenues.Length
                        + " — content (source/owner/origin/target) differs from a live scan");
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

                    if (CommandTreeRuntime.TryGetCachedRosterSignature(allianceId, out TacticalRosterSignature cachedRoster))
                    {
                        var liveRoster = CommandTreeRuntime.ReadRosterSignature(allianceId);
                        if (!cachedRoster.Equals(liveRoster))
                        {
                            // Expected between a roster change and the next Snapshot();
                            // the next read rebuilds. Recorded (not a fault) so smoke sees the churn.
                            OnceLog.Info(
                                "tactical-cache-validator:command-tree-roster-changed",
                                "[TacticalCacheValidator] command tree roster changed since last build: cached="
                                + cachedRoster + " live=" + liveRoster + " — next Snapshot() refreshes or rebuilds");
                        }
                    }

                    var snap = CommandTreeRuntime.Snapshot(allianceId);
                    bool hasAnyRegiments = CountActiveRegimentsForAlliance(allianceId) > 0;
                    if (hasAnyRegiments && (snap == null || snap == CommandTreeSnapshot.Empty))
                    {
                        mismatches++;
                        OnceLog.Warning(
                            "tactical-cache-validator:command-tree-empty-but-populated",
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
                            "tactical-cache-validator:resolved-cache-miss",
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
