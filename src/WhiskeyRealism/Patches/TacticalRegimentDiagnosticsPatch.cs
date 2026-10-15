using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Per-regiment delta logger for diagnosing stuck-retreat / stuck-charge / unit-state bugs
    // that the matrix observer (#35) cannot see because it only tracks brigade (type 14)
    // and division (type 15) groups.
    //
    // Watch-list-driven (Plugin.TacticalRegimentDiagnosticNames is a comma-separated case-
    // insensitive substring list; empty list = no units logged even when feature enabled).
    // Delta-only: only logs when interesting state changes vs. the last cached snapshot
    // for that unit. Per-unit min-sample-interval prevents spam from rapid state churn.
    // Hard-capped at MaxLinesPerBattle to bound worst-case log size.
    //
    // Reset between battles via TacticalBattleCoordinator.OnBattleEnd → ClearLedgersBetweenBattles.
    //
    // Cost (WS P, P-regdiag): hosted on AIBattle.CheckGlobalAIStrategy (decompile 6314, the
    // macro AI cycle). An empty watch list returns before touching BattleUnits. Matched
    // regiments are cached per battle; BattleUnits.completeunitlist (78920) is re-walked
    // (reading .name for every unit) only when its Count or the watch list changes.
    [HarmonyPatch]
    internal static class TacticalRegimentDiagnosticsPatch
    {
        private const int MaxLinesPerBattle = 2000;
        private const float MinSecondsBetweenSamples = 0.5f;
        private const float MoraleDeltaThreshold = 0.05f;
        private const int StrengthDeltaThreshold = 10;
        private const float PositionDeltaThreshold = 1.0f;

        private sealed class Snapshot
        {
            public int aiStance;
            public int aiStanceOrdered;
            public bool inBattle;
            public bool onRetreat;
            public bool isRouted;
            public int regimentPaths;
            public bool pathInterrupted;
            public int orderState;
            public int movementMode;
            public float morale;
            public int strength;
            public float posX;
            public float posZ;
            public string parentName;
            public float lastLogSeconds;
        }

        private struct WatchedUnit
        {
            public Regiment Unit;
            public string Name;
            public int InstanceId;
        }

        private static readonly Dictionary<int, Snapshot> _last = new Dictionary<int, Snapshot>();
        private static readonly List<WatchedUnit> _watched = new List<WatchedUnit>();
        private static int _logCount;
        private static string[] _watchTokensCache;
        private static string _watchTokensSource;
        private static bool _watchedValid;
        private static int _watchedRosterCount;
        private static string _watchedSource;

        public static void Reset()
        {
            _last.Clear();
            _logCount = 0;
            _watchTokensCache = null;
            _watchTokensSource = null;
            _watched.Clear();
            _watchedValid = false;
            _watchedRosterCount = 0;
            _watchedSource = null;
        }

        [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
        [HarmonyPostfix]
        internal static void CheckGlobalAIStrategyPostfix()
        {
            using (TelemetryPerf.Scope("tactical.patch.regiment-diag", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (!Plugin.EnableTacticalRegimentDiagnostics.Value) return;
                    if (_logCount >= MaxLinesPerBattle) return;

                    string source = Plugin.TacticalRegimentDiagnosticNames != null
                        ? Plugin.TacticalRegimentDiagnosticNames.Value
                        : null;
                    var watchTokens = ResolveWatchTokens(source);
                    if (watchTokens.Length == 0) return;

                    var all = BattleUnits.completeunitlist;
                    if (all == null || all.Count == 0) return;

                    if (ShouldRescan(_watchedValid, _watchedRosterCount, all.Count, _watchedSource, source))
                        RebuildWatched(all, watchTokens, source);
                    if (_watched.Count == 0) return;

                    float now = Time.time;

                    for (int i = 0; i < _watched.Count && _logCount < MaxLinesPerBattle; i++)
                    {
                        var watched = _watched[i];
                        var r = watched.Unit;
                        if (r == null) continue;

                        int key = watched.InstanceId;
                        Snapshot prev;
                        bool isNew = !_last.TryGetValue(key, out prev);
                        if (!isNew && (now - prev.lastLogSeconds) < MinSecondsBetweenSamples) continue;

                        var current = SnapshotOf(r, now);
                        if (current == null) continue;

                        bool changed = isNew || HasChanged(prev, current);
                        if (!changed) continue;

                        Log(watched.Name, key, prev, current, isNew);
                        _last[key] = current;
                        _logCount++;
                    }
                }
                catch (Exception e)
                {
                    OnceLog.Warning(
                        "tactical-regiment-diag:failed",
                        "[TacticalRegimentDiagnostics] " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        internal static bool ShouldRescan(bool hasCache, int cachedRosterCount, int rosterCount, string cachedSource, string source)
        {
            return !hasCache
                || cachedRosterCount != rosterCount
                || !string.Equals(cachedSource, source, StringComparison.Ordinal);
        }

        private static void RebuildWatched(List<Regiment> all, string[] watchTokens, string source)
        {
            _watched.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;

                string name = SafeName(r);
                if (!MatchesAnyToken(name, watchTokens)) continue;

                int key;
                try { key = r.GetInstanceID(); } catch { continue; }
                _watched.Add(new WatchedUnit { Unit = r, Name = name, InstanceId = key });
            }

            _watchedValid = true;
            _watchedRosterCount = all.Count;
            _watchedSource = source;
        }

        private static string[] ResolveWatchTokens(string source)
        {
            if (_watchTokensCache != null && string.Equals(source, _watchTokensSource, StringComparison.Ordinal))
                return _watchTokensCache;

            _watchTokensCache = ParseWatchTokens(source);
            _watchTokensSource = source;
            return _watchTokensCache;
        }

        internal static string[] ParseWatchTokens(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return new string[0];

            var parts = source.Split(',');
            var list = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i] != null ? parts[i].Trim() : null;
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            return list.ToArray();
        }

        internal static bool MatchesAnyToken(string name, string[] tokens)
        {
            if (string.IsNullOrEmpty(name) || tokens == null) return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (name.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string SafeName(Regiment r)
        {
            try { return ((UnityEngine.Object)r).name; }
            catch { return null; }
        }

        private static Snapshot SnapshotOf(Regiment r, float now)
        {
            try
            {
                var snap = new Snapshot { lastLogSeconds = now };
                try { snap.aiStance = r.ai_stance; } catch { }
                try { snap.aiStanceOrdered = r.ai_stanceordered; } catch { }
                try { snap.inBattle = r.inbattle; } catch { }
                try { snap.onRetreat = r.onretreat; } catch { }
                try { snap.isRouted = r.isrouted; } catch { }
                try { snap.regimentPaths = r.regimentpaths; } catch { }
                try { snap.pathInterrupted = r.pathinterrupted; } catch { }
                try { snap.orderState = r.orderstate; } catch { }
                try { snap.movementMode = r.movementmode; } catch { }
                try { snap.morale = r.groupmorale; } catch { }
                try { snap.strength = r.groupstrengthactive; } catch { }
                try
                {
                    var pos = ((Component)r).transform.position;
                    snap.posX = pos.x;
                    snap.posZ = pos.z;
                }
                catch { }
                try
                {
                    var parent = ((Component)r).transform.parent;
                    if ((object)parent != null) snap.parentName = parent.name;
                }
                catch { }
                return snap;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasChanged(Snapshot a, Snapshot b)
        {
            if (a == null || b == null) return true;
            if (a.aiStance != b.aiStance) return true;
            if (a.aiStanceOrdered != b.aiStanceOrdered) return true;
            if (a.inBattle != b.inBattle) return true;
            if (a.onRetreat != b.onRetreat) return true;
            if (a.isRouted != b.isRouted) return true;
            if (a.regimentPaths != b.regimentPaths) return true;
            if (a.pathInterrupted != b.pathInterrupted) return true;
            if (a.orderState != b.orderState) return true;
            if (a.movementMode != b.movementMode) return true;
            if (Math.Abs(a.morale - b.morale) > MoraleDeltaThreshold) return true;
            if (Math.Abs(a.strength - b.strength) > StrengthDeltaThreshold) return true;
            if (Math.Abs(a.posX - b.posX) > PositionDeltaThreshold) return true;
            if (Math.Abs(a.posZ - b.posZ) > PositionDeltaThreshold) return true;
            return false;
        }

        private static void Log(string name, int instanceId, Snapshot prev, Snapshot curr, bool isNew)
        {
            // Format: full snapshot on first observation, only changed fields on subsequent.
            var sb = new System.Text.StringBuilder(256);
            sb.Append("[TacticalRegimentTrace] t=").Append(curr.lastLogSeconds.ToString("F1"));
            sb.Append(" unit=").Append(name).Append('#').Append(instanceId);
            if (!string.IsNullOrEmpty(curr.parentName)) sb.Append(" parent=").Append(curr.parentName);

            if (isNew || prev == null)
            {
                sb.Append(" [first]");
                sb.Append(" ai=").Append(curr.aiStance);
                sb.Append(" ord=").Append(curr.aiStanceOrdered);
                sb.Append(" inbattle=").Append(curr.inBattle);
                sb.Append(" onret=").Append(curr.onRetreat);
                sb.Append(" rout=").Append(curr.isRouted);
                sb.Append(" paths=").Append(curr.regimentPaths);
                sb.Append(" pathInt=").Append(curr.pathInterrupted);
                sb.Append(" ord=").Append(curr.orderState);
                sb.Append(" mov=").Append(curr.movementMode);
                sb.Append(" morale=").Append(curr.morale.ToString("F2"));
                sb.Append(" strength=").Append(curr.strength);
                sb.Append(" pos=(").Append(curr.posX.ToString("F1")).Append(',').Append(curr.posZ.ToString("F1")).Append(')');
            }
            else
            {
                if (prev.aiStance != curr.aiStance) sb.Append(" ai=").Append(prev.aiStance).Append("->").Append(curr.aiStance);
                if (prev.aiStanceOrdered != curr.aiStanceOrdered) sb.Append(" ord=").Append(prev.aiStanceOrdered).Append("->").Append(curr.aiStanceOrdered);
                if (prev.inBattle != curr.inBattle) sb.Append(" inbattle=").Append(prev.inBattle).Append("->").Append(curr.inBattle);
                if (prev.onRetreat != curr.onRetreat) sb.Append(" onret=").Append(prev.onRetreat).Append("->").Append(curr.onRetreat);
                if (prev.isRouted != curr.isRouted) sb.Append(" rout=").Append(prev.isRouted).Append("->").Append(curr.isRouted);
                if (prev.regimentPaths != curr.regimentPaths) sb.Append(" paths=").Append(prev.regimentPaths).Append("->").Append(curr.regimentPaths);
                if (prev.pathInterrupted != curr.pathInterrupted) sb.Append(" pathInt=").Append(prev.pathInterrupted).Append("->").Append(curr.pathInterrupted);
                if (prev.orderState != curr.orderState) sb.Append(" orderState=").Append(prev.orderState).Append("->").Append(curr.orderState);
                if (prev.movementMode != curr.movementMode) sb.Append(" mov=").Append(prev.movementMode).Append("->").Append(curr.movementMode);
                if (Math.Abs(prev.morale - curr.morale) > MoraleDeltaThreshold) sb.Append(" morale=").Append(prev.morale.ToString("F2")).Append("->").Append(curr.morale.ToString("F2"));
                if (Math.Abs(prev.strength - curr.strength) > StrengthDeltaThreshold) sb.Append(" strength=").Append(prev.strength).Append("->").Append(curr.strength);
                if (Math.Abs(prev.posX - curr.posX) > PositionDeltaThreshold || Math.Abs(prev.posZ - curr.posZ) > PositionDeltaThreshold)
                {
                    sb.Append(" pos=(").Append(prev.posX.ToString("F1")).Append(',').Append(prev.posZ.ToString("F1"))
                      .Append(")->(").Append(curr.posX.ToString("F1")).Append(',').Append(curr.posZ.ToString("F1")).Append(')');
                }
            }

            TelemetryRouter.LegacyInfo(sb.ToString(), TelemetryLayer.Tactical);

            if (_logCount + 1 >= MaxLinesPerBattle)
            {
                OnceLog.Warning(
                    "tactical-regiment-diag:line-cap",
                    "[TacticalRegimentDiagnostics] hit MaxLinesPerBattle=" + MaxLinesPerBattle + "; suppressing further regiment trace lines until next battle.");
            }
        }
    }
}
