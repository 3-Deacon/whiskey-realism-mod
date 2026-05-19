using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Patches
{
    // Read-only tactical map-knowledge probe. This records what vanilla scene anchors
    // exist for the current battle and what nearby objective/entry/fortification/cover
    // context a watched regiment can see. It never changes orders, facing, formation, or paths.
    [HarmonyPatch]
    internal static class TacticalMapKnowledgeDiagnosticsPatch
    {
        private const int MaxUnitRowsPerBattle = 1000;
        private const float MinSecondsBetweenUnitSamples = 2.0f;
        private const float ThreatSearchMeters = 2500f;

        private struct Anchor
        {
            public string Name;
            public Vector3 Position;
        }

        private sealed class UnitEmission
        {
            public float LastSeconds;
            public string Signature;
        }

        private static readonly Dictionary<int, UnitEmission> UnitRows = new Dictionary<int, UnitEmission>();
        private static bool _inventoryEmitted;
        private static int _unitRowsEmitted;
        private static string[] _watchTokensCache;
        private static string _watchTokensSource;

        public static void Reset()
        {
            UnitRows.Clear();
            _inventoryEmitted = false;
            _unitRowsEmitted = 0;
            _watchTokensCache = null;
            _watchTokensSource = null;
        }

        [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
        [HarmonyPostfix]
        internal static void CheckGlobalAIStrategyPostfix()
        {
            using (TelemetryPerf.Scope("tactical.patch.map-knowledge-diag", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (Plugin.EnableTacticalMapKnowledgeDiagnostics == null || !Plugin.EnableTacticalMapKnowledgeDiagnostics.Value)
                        return;

                    if (!_inventoryEmitted)
                    {
                        EmitInventory();
                        _inventoryEmitted = true;
                    }

                    EmitWatchedUnitContexts();
                }
                catch (Exception e)
                {
                    try { Plugin.Log.LogWarning("[TacticalMapKnowledge] failed " + e.GetType().Name + ": " + e.Message); } catch { }
                }
            }
        }

        private static void EmitInventory()
        {
            var objectives = ObjectiveAnchors();
            var entryPoints = EntryPointAnchors();
            var fortifications = FortificationAnchors();

            int aiFortificationRoots = 0;
            try { aiFortificationRoots = GameObject.Find("Battlemap/Map/AIFortifications") != null ? 1 : 0; } catch { }

            var row = new TacticalMapKnowledgeInventoryRow(
                "CheckGlobalAIStrategy",
                objectives.Count,
                entryPoints.Count,
                aiFortificationRoots,
                fortifications.Count,
                CountTagged("Road"),
                CountTagged("Railroad"),
                SampleName(objectives),
                SampleName(entryPoints),
                SampleName(fortifications));

            TelemetryRouter.LegacyInfo(TacticalMapKnowledgeTelemetry.FormatInventory(row), TelemetryLayer.Tactical);
        }

        private static void EmitWatchedUnitContexts()
        {
            if (_unitRowsEmitted >= MaxUnitRowsPerBattle)
                return;

            if (Plugin.EnableTacticalRegimentDiagnostics == null || !Plugin.EnableTacticalRegimentDiagnostics.Value)
                return;

            var tokens = ResolveWatchTokens();
            if (tokens == null || tokens.Length == 0)
                return;

            var all = BattleUnits.completeunitlist;
            if (all == null || all.Count == 0)
                return;

            var objectives = ObjectiveAnchors();
            var entryPoints = EntryPointAnchors();
            var fortifications = FortificationAnchors();
            float now = Time.time;

            for (int i = 0; i < all.Count && _unitRowsEmitted < MaxUnitRowsPerBattle; i++)
            {
                var unit = all[i];
                if (unit == null)
                    continue;

                string name = SafeUnitName(unit);
                if (!MatchesAnyToken(name, tokens))
                    continue;

                int key;
                try { key = unit.GetInstanceID(); } catch { continue; }

                UnitEmission previous;
                if (UnitRows.TryGetValue(key, out previous) && now - previous.LastSeconds < MinSecondsBetweenUnitSamples)
                    continue;

                var row = BuildUnitRow(unit, name, objectives, entryPoints, fortifications);
                string signature = TacticalMapKnowledgeTelemetry.UnitSignature(row);
                if (previous != null && previous.Signature == signature)
                {
                    previous.LastSeconds = now;
                    continue;
                }

                TelemetryRouter.LegacyInfo(TacticalMapKnowledgeTelemetry.FormatUnitContext(row), TelemetryLayer.Tactical);
                UnitRows[key] = new UnitEmission { LastSeconds = now, Signature = signature };
                _unitRowsEmitted++;
            }
        }

        private static TacticalMapKnowledgeUnitContextRow BuildUnitRow(
            Regiment unit,
            string name,
            IReadOnlyList<Anchor> objectives,
            IReadOnlyList<Anchor> entryPoints,
            IReadOnlyList<Anchor> fortifications)
        {
            Vector3 position = SafePosition(unit);
            Anchor nearestObjective = Nearest(position, objectives);
            Anchor nearestEntryPoint = Nearest(position, entryPoints);
            Anchor nearestFortification = Nearest(position, fortifications);

            Vector3 threatPosition;
            string threatConfidence;
            bool hasThreat = TryThreatPosition(unit, out threatPosition, out threatConfidence);

            return new TacticalMapKnowledgeUnitContextRow(
                name,
                SafeAlliance(unit),
                position.x,
                position.z,
                SafeFacing(unit),
                SafeAiStance(unit),
                SafeFormation(unit),
                SafeCurrentObjective(unit),
                nearestObjective.Name,
                Bearing(position, nearestObjective.Position),
                Distance(position, nearestObjective.Position),
                nearestEntryPoint.Name,
                Bearing(position, nearestEntryPoint.Position),
                Distance(position, nearestEntryPoint.Position),
                nearestFortification.Name,
                Bearing(position, nearestFortification.Position),
                Distance(position, nearestFortification.Position),
                SafeCoverValue(unit),
                SafeCoverValueSubordinates(unit),
                SafeCoverObject(unit),
                hasThreat ? Bearing(position, threatPosition) : 0f,
                hasThreat ? Distance(position, threatPosition) : 0f,
                threatConfidence);
        }

        private static List<Anchor> ObjectiveAnchors()
        {
            var list = new List<Anchor>();
            try
            {
                var objectives = BattleUnits.objectives;
                if (objectives != null)
                {
                    for (int i = 0; i < objectives.Length; i++)
                        AddObjective(list, objectives[i]);
                }
            }
            catch { }

            if (list.Count == 0)
            {
                try
                {
                    var folder = GameObject.Find("Battlemap/Map/Objectives");
                    var objectives = folder != null ? folder.GetComponentsInChildren<Objectives>(true) : null;
                    if (objectives != null)
                    {
                        for (int i = 0; i < objectives.Length; i++)
                            AddObjective(list, objectives[i]);
                    }
                }
                catch { }
            }

            return list;
        }

        private static void AddObjective(List<Anchor> list, Objectives objective)
        {
            try
            {
                if (objective == null) return;
                list.Add(new Anchor
                {
                    Name = !string.IsNullOrWhiteSpace(objective.objectivename)
                        ? objective.objectivename
                        : ((UnityEngine.Object)objective).name,
                    Position = ((Component)objective).transform.position
                });
            }
            catch { }
        }

        private static List<Anchor> EntryPointAnchors()
        {
            var list = new List<Anchor>();
            try
            {
                var controller = GameObject.Find("GameController");
                var bunits = controller != null ? controller.GetComponent<BattleUnits>() : null;
                var entryPoints = bunits != null ? bunits.entrypoints : null;
                if (entryPoints != null)
                {
                    for (int i = 0; i < entryPoints.Length; i++)
                        AddEntryPoint(list, entryPoints[i]);
                }
            }
            catch { }

            if (list.Count == 0)
            {
                AddEntryPointsFromFolder(list, "Battlemap/Map/Entry Points");
                AddEntryPointsFromFolder(list, "Battlemap/Map/EntryPoints");
            }

            return list;
        }

        private static void AddEntryPointsFromFolder(List<Anchor> list, string path)
        {
            try
            {
                var folder = GameObject.Find(path);
                var entryPoints = folder != null ? folder.GetComponentsInChildren<EntryPoint>(true) : null;
                if (entryPoints != null)
                {
                    for (int i = 0; i < entryPoints.Length; i++)
                        AddEntryPoint(list, entryPoints[i]);
                }
            }
            catch { }
        }

        private static void AddEntryPoint(List<Anchor> list, EntryPoint entryPoint)
        {
            try
            {
                if (entryPoint == null) return;
                list.Add(new Anchor
                {
                    Name = ((UnityEngine.Object)entryPoint).name,
                    Position = ((Component)entryPoint).transform.position
                });
            }
            catch { }
        }

        private static List<Anchor> FortificationAnchors()
        {
            var list = new List<Anchor>();
            try
            {
                var groups = UnityEngine.Object.FindObjectsOfType<FortificationGroup>();
                if (groups != null)
                {
                    for (int i = 0; i < groups.Length; i++)
                    {
                        var group = groups[i];
                        if (group == null) continue;
                        list.Add(new Anchor
                        {
                            Name = ((UnityEngine.Object)group).name,
                            Position = ((Component)group).transform.position
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        private static int CountTagged(string tag)
        {
            try
            {
                var objects = GameObject.FindGameObjectsWithTag(tag);
                return objects != null ? objects.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string[] ResolveWatchTokens()
        {
            string source = Plugin.TacticalRegimentDiagnosticNames != null
                ? Plugin.TacticalRegimentDiagnosticNames.Value
                : null;
            if (string.IsNullOrEmpty(source)) return null;
            if (source == _watchTokensSource && _watchTokensCache != null) return _watchTokensCache;

            var parts = source.Split(',');
            var list = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i] != null ? parts[i].Trim() : null;
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }

            _watchTokensSource = source;
            _watchTokensCache = list.ToArray();
            return _watchTokensCache;
        }

        private static bool MatchesAnyToken(string name, string[] tokens)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (name.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static Anchor Nearest(Vector3 origin, IReadOnlyList<Anchor> anchors)
        {
            if (anchors == null || anchors.Count == 0)
                return new Anchor { Name = "-", Position = origin };

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                float distance = Distance(origin, anchors[i].Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 ? anchors[best] : new Anchor { Name = "-", Position = origin };
        }

        private static string SampleName(IReadOnlyList<Anchor> anchors)
        {
            return anchors != null && anchors.Count > 0 ? anchors[0].Name : "-";
        }

        private static string SafeUnitName(Regiment unit)
        {
            try { return ((UnityEngine.Object)unit).name; }
            catch { return "-"; }
        }

        private static Vector3 SafePosition(Regiment unit)
        {
            try { return ((Component)unit).transform.position; }
            catch { return Vector3.zero; }
        }

        private static int SafeAlliance(Regiment unit)
        {
            try { return unit.alliance; } catch { return -1; }
        }

        private static float SafeFacing(Regiment unit)
        {
            try { return ((Component)unit).transform.eulerAngles.y; }
            catch { return 0f; }
        }

        private static int SafeAiStance(Regiment unit)
        {
            try { return unit.ai_stance; } catch { return -1; }
        }

        private static int SafeFormation(Regiment unit)
        {
            try { return unit.formation; } catch { return -1; }
        }

        private static string SafeCurrentObjective(Regiment unit)
        {
            try
            {
                var objective = unit.currentsetobjective;
                if (objective == null) return "-";
                return !string.IsNullOrWhiteSpace(objective.objectivename)
                    ? objective.objectivename
                    : ((UnityEngine.Object)objective).name;
            }
            catch { return "-"; }
        }

        private static float SafeCoverValue(Regiment unit)
        {
            try { return unit.covervalue; } catch { return 0f; }
        }

        private static float SafeCoverValueSubordinates(Regiment unit)
        {
            try { return unit.covervaluesubordinates; } catch { return 0f; }
        }

        private static int SafeCoverObject(Regiment unit)
        {
            try { return unit.coverobject; } catch { return -1; }
        }

        private static bool TryThreatPosition(Regiment unit, out Vector3 position, out string confidence)
        {
            position = Vector3.zero;
            confidence = "none";
            try
            {
                var enemy = unit.GetClosestEnemyUnit(ThreatSearchMeters);
                if (enemy == null) return false;
                position = enemy.transform.position;
                confidence = "visible-enemy";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float Bearing(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f) return 0f;
            float angle = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
            angle %= 360f;
            return angle < 0f ? angle + 360f : angle;
        }

        private static float Distance(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
