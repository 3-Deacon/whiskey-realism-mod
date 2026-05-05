using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class FrontSectorRuntime
    {
        private sealed class Accumulator
        {
            internal float Own;
            internal float Enemy;
            internal float Morale;
            internal float Supply;
            internal float Readiness;
            internal int OwnCount;
        }

        internal static FrontSectorLedger BuildForAlliance(int allianceId, int targetObjectiveId)
        {
            try
            {
                var faction = FindFaction(allianceId);
                if (faction == null)
                {
                    OnceLog.Warning(
                        "front-ledger:no-faction:" + allianceId,
                        $"[FrontLedger] build skipped: AICampaign faction not found for alliance={allianceId}");
                    return null;
                }

                var factionType = faction.GetType();
                var ownUnits = AccessTools.Field(factionType, "ownunits")?.GetValue(faction) as IList;
                var enemyUnits = AccessTools.Field(factionType, "enemyunits")?.GetValue(faction) as IList;
                if (ownUnits == null || enemyUnits == null)
                {
                    OnceLog.Warning(
                        "front-ledger:no-unit-lists:" + allianceId,
                        $"[FrontLedger] build skipped: ownunits/enemyunits unavailable for alliance={allianceId}");
                    return null;
                }

                var targetTheater = Theater.Unknown;
                var targetPosition = ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId);
                if (targetPosition.HasValue)
                    targetTheater = TheaterFromPosition(targetPosition.Value);

                var acc = new Dictionary<Theater, Accumulator>();
                AddUnits(acc, ownUnits, own: true);
                AddUnits(acc, enemyUnits, own: false);

                var inputs = new List<FrontSectorInput>();
                foreach (Theater theater in new[] { Theater.East, Theater.West, Theater.TransMiss, Theater.Coast, Theater.River, Theater.Unknown })
                {
                    acc.TryGetValue(theater, out var a);
                    a = a ?? new Accumulator();

                    float importance = BaseImportance(theater);
                    bool isPlanTarget = targetTheater == theater && targetObjectiveId >= 0;
                    if (isPlanTarget) importance = Math.Max(importance, 0.95f);

                    inputs.Add(new FrontSectorInput
                    {
                        SectorKey = SectorKey(theater),
                        Theater = theater,
                        OwnStrength = a.Own,
                        EnemyStrength = a.Enemy,
                        StrategicImportance = importance,
                        IsCritical = IsCritical(theater) || isPlanTarget,
                        IsPlanTarget = isPlanTarget,
                        CommanderAudacity = 0f,
                        CommanderCaution = 0f,
                        AverageMorale = a.OwnCount > 0 ? a.Morale / a.OwnCount : 1f,
                        AverageSupply = a.OwnCount > 0 ? a.Supply / a.OwnCount : 1f,
                        AverageReadiness = a.OwnCount > 0 ? a.Readiness / a.OwnCount : 1f
                    });
                }

                return FrontSectorLedger.Build(inputs);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("front-ledger:build", "[FrontLedger] build failed: " + ex.Message);
                return null;
            }
        }

        internal static string SectorKey(Vector3 position)
        {
            return SectorKey(TheaterFromPosition(position));
        }

        internal static string Summary(FrontSectorLedger ledger)
        {
            if (ledger == null || ledger.Sectors.Count == 0) return "<none>";
            var parts = new List<string>();
            foreach (var sector in ledger.Sectors)
            {
                if (sector.OwnStrength <= 0f && sector.EnemyStrength <= 0f) continue;
                parts.Add($"{sector.SectorKey}:{sector.Posture}:{sector.StrengthRatio:F2}");
            }
            return parts.Count == 0 ? "<none>" : string.Join(",", parts);
        }

        // Delegates to FrontSectorLedger.Signature() for testability — the logic lives on the
        // ledger type (which has no Unity/Harmony dependencies) so the console test harness can
        // call it directly without mocking HarmonyX reflection machinery.
        internal static string Signature(FrontSectorLedger ledger)
        {
            if (ledger == null) return "empty";
            return ledger.Signature();
        }

        private static object FindFaction(int allianceId)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var faction = list[i];
                var field = AccessTools.Field(faction.GetType(), "allianceid");
                if (field != null && Convert.ToInt32(field.GetValue(faction)) == allianceId)
                    return faction;
            }
            return null;
        }

        private static void AddUnits(Dictionary<Theater, Accumulator> acc, IList units, bool own)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                var pos = UnitPosition(unit);
                if (!pos.HasValue) continue;

                var theater = TheaterFromPosition(pos.Value);
                if (!acc.TryGetValue(theater, out var a))
                {
                    a = new Accumulator();
                    acc[theater] = a;
                }

                float strength = ReadFloat(unit, "groupstrengthactive", "groupstrengthdirect", "groupstrength", "strength");
                if (own)
                {
                    a.Own += strength;
                    a.Morale += Clamp01(ReadFloat(1f, unit, "groupmorale", "morale"));
                    a.Supply += Clamp01(ReadSupply(unit));
                    a.Readiness += Clamp01(ReadFloat(1f, unit, "readiness"));
                    a.OwnCount++;
                }
                else
                {
                    a.Enemy += strength;
                }
            }
        }

        private static Vector3? UnitPosition(object unit)
        {
            if (unit is Component component) return component.transform.position;
            try
            {
                var method = AccessTools.Method(unit.GetType(), "GetPosition");
                if (method != null) return (Vector3)method.Invoke(unit, null);
            }
            catch { }
            return null;
        }

        private static float ReadSupply(object unit)
        {
            try
            {
                var field = AccessTools.Field(unit.GetType(), "groupsupplystate");
                var values = field?.GetValue(unit) as float[];
                if (values != null && values.Length > 0)
                {
                    float total = 0f;
                    for (int i = 0; i < values.Length; i++) total += values[i];
                    return total / values.Length;
                }
            }
            catch { }
            return ReadFloat(1f, unit, "supplystate");
        }

        private static float ReadFloat(object target, params string[] fields)
        {
            return ReadFloat(0f, target, fields);
        }

        private static float ReadFloat(float fallback, object target, params string[] fields)
        {
            if (target == null) return 0f;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = AccessTools.Field(target.GetType(), fields[i]);
                if (field == null) continue;
                try { return Math.Max(0f, Convert.ToSingle(field.GetValue(target))); }
                catch { return 0f; }
            }
            return fallback;
        }

        private static Theater TheaterFromPosition(Vector3 position)
        {
            return CampaignMapTheaterRuntime.FromPosition(position);
        }

        private static string SectorKey(Theater theater)
        {
            return theater.ToString();
        }

        private static bool IsCritical(Theater theater)
        {
            return theater == Theater.East || theater == Theater.River;
        }

        private static float BaseImportance(Theater theater)
        {
            switch (theater)
            {
                case Theater.East: return 0.95f;
                case Theater.River: return 0.9f;
                case Theater.West: return 0.7f;
                case Theater.Coast: return 0.6f;
                case Theater.TransMiss: return 0.35f;
                default: return 0.2f;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
