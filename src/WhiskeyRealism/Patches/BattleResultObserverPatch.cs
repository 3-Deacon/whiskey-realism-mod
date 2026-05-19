using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 battle-history observer. Vanilla writes final battle outcome data
    // through BattleMonument.UpdateAllianceWon after battle-result import.
    // We record only final result updates (_alliancewon >= 0), not initial
    // battle-start monument creation, so open battles do not enter history.
    [HarmonyPatch]
    internal static class BattleResultObserverPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("BattleMonument");
            if (t == null)
            {
                OnceLog.Warning("battle-result:target-type", "BattleResultObserverPatch target type BattleMonument not found");
                return null;
            }

            var method = AccessTools.Method(t, "UpdateAllianceWon");
            if (method == null)
                OnceLog.Warning("battle-result:target-method", "BattleResultObserverPatch target method BattleMonument.UpdateAllianceWon not found");
            return method;
        }

        [HarmonyPostfix]
        internal static void Postfix(
            object __instance,
            int _alliancewon = -1)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-result-observer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (_alliancewon < 0) return;

                try
                {
                    OnceLog.Info("battle-result", "BattleResultObserverPatch wired");

                    var record = BuildRecord(__instance);
                    if (record == null || !record.IsLandBattle || record.AllianceWon < 0) return;

                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    StrategicCoordinator.Instance.RecordBattleOutcome(record);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[BattleResultObserverPatch] " + ex.Message);
                }
            }
        }

        private static BattleHistoryRecord BuildRecord(object monument)
        {
            if (monument == null) return null;
            var type = monument.GetType();

            var record = new BattleHistoryRecord
            {
                BattleName = Read<string>(type, monument, "battlename") ?? "",
                LandOrSea = Read<int>(type, monument, "landorsea"),
                AllianceWon = Read<int>(type, monument, "alliancewon"),
                BattleResultType = Read<int>(type, monument, "battleresulttype"),
                BattleEndType = Read<int>(type, monument, "battleendtype"),
                Alliance = ReadArray<int>(type, monument, "alliance", 2, -1),
                Commander = ReadArray<int>(type, monument, "commander", 2, -1)
            };

            var pos = Read<Vector3>(type, monument, "position");
            record.PositionX = pos.x;
            record.PositionZ = pos.z;
            record.Theater = BucketTheaterFromWorldXY(pos.x, pos.z);

            var date = AccessTools.Field(type, "dateto")?.GetValue(monument);
            if (date != null)
            {
                var dateType = date.GetType();
                record.Day = Read<int>(dateType, date, "day");
                record.Month = Read<int>(dateType, date, "month");
                record.Year = Read<int>(dateType, date, "year");
            }
            else
            {
                OnceLog.Warning("battle-result:field:dateto", "BattleResultObserverPatch missing BattleMonument.dateto; battle date will be 0-00-00");
            }

            var casualties = AccessTools.Field(type, "casualties")?.GetValue(monument) as int[,];
            if (casualties != null && casualties.GetLength(0) >= 2 && casualties.GetLength(1) >= 6)
            {
                record.Casualties[0] = casualties[0, 5];
                record.Casualties[1] = casualties[1, 5];
            }
            else
            {
                OnceLog.Warning("battle-result:field:casualties", "BattleResultObserverPatch missing BattleMonument.casualties[2,6]; casualty totals unavailable");
            }

            AddAll(record.CommanderKia, AccessTools.Field(type, "commanderkia0")?.GetValue(monument) as IList<int>);
            AddAll(record.CommanderKia, AccessTools.Field(type, "commanderkia1")?.GetValue(monument) as IList<int>);

            record.CommanderName[0] = CommanderName(record.Commander[0]);
            record.CommanderName[1] = CommanderName(record.Commander[1]);

            return record;
        }

        private static T Read<T>(Type type, object instance, string fieldName)
        {
            var field = AccessTools.Field(type, fieldName);
            if (field == null)
                OnceLog.Warning("battle-result:field:" + type.Name + "." + fieldName,
                    "BattleResultObserverPatch missing " + type.Name + "." + fieldName);
            if (field == null) return default(T);
            var value = field.GetValue(instance);
            return value is T typed ? typed : default(T);
        }

        private static T[] ReadArray<T>(Type type, object instance, string fieldName, int length, T fallback)
        {
            var result = new T[length];
            for (int i = 0; i < result.Length; i++) result[i] = fallback;

            var field = AccessTools.Field(type, fieldName);
            if (field == null)
            {
                OnceLog.Warning("battle-result:field:" + type.Name + "." + fieldName,
                    "BattleResultObserverPatch missing " + type.Name + "." + fieldName);
                return result;
            }

            var arr = field.GetValue(instance) as T[];
            if (arr == null)
            {
                OnceLog.Warning("battle-result:field:" + type.Name + "." + fieldName + ":shape",
                    "BattleResultObserverPatch could not read " + type.Name + "." + fieldName + " as expected array");
                return result;
            }
            for (int i = 0; i < result.Length && i < arr.Length; i++) result[i] = arr[i];
            return result;
        }

        private static void AddAll(List<int> target, IList<int> source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < source.Count; i++)
                if (source[i] >= 0) target.Add(source[i]);
        }

        private static string CommanderName(int commanderId)
        {
            try
            {
                var gv = AccessTools.TypeByName("GameVars");
                var commanders = AccessTools.Field(gv, "commander")?.GetValue(null) as System.Collections.IList;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return "";
                var commander = commanders[commanderId];
                return AccessTools.Field(commander.GetType(), "combinedname")?.GetValue(commander) as string
                    ?? AccessTools.Field(commander.GetType(), "name")?.GetValue(commander) as string
                    ?? "";
            }
            catch { return ""; }
        }

        private static Theater BucketTheaterFromWorldXY(float x, float z)
        {
            if (x < -200f) return Theater.TransMiss;
            if (x > 800f && z < -100f) return Theater.Coast;
            if (x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
