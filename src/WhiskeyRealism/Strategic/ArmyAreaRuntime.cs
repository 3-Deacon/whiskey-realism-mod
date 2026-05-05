using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class ArmyAreaRuntime
    {
        private static readonly Dictionary<string, Vector3> Anchors = new Dictionary<string, Vector3>
        {
            { "VirginiaCapitalCorridor", new Vector3(1263f, 0f, -1010f) },
            { "WashingtonDefenses", new Vector3(1350f, 0f, -631f) },
            { "ShenandoahValley", new Vector3(1107f, 0f, -555f) },
            { "NorthwestVirginia", new Vector3(702f, 0f, -696f) },
            { "MarylandPennsylvaniaCorridor", new Vector3(1195f, 0f, -426f) },
            { "CoastalCarolinaVirginia", new Vector3(1451f, 0f, -1233f) },
            { "CarolinaInterior", new Vector3(750f, 0f, -1600f) },
            { "OhioValley", new Vector3(400f, 0f, -340f) }
        };

        internal static ArmyAreaLedger BuildForAlliance(int allianceId, string planTargetAreaKey)
        {
            try
            {
                var faction = FindFaction(allianceId);
                if (faction == null)
                {
                    OnceLog.Warning(
                        "army-area:no-faction:" + allianceId,
                        $"[ArmyArea] build skipped: AICampaign faction not found for alliance={allianceId}");
                    return null;
                }

                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                if (ownUnits == null)
                {
                    OnceLog.Warning(
                        "army-area:no-ownunits:" + allianceId,
                        $"[ArmyArea] build skipped: ownunits unavailable for alliance={allianceId}");
                    return null;
                }

                var inputs = new List<ArmyAreaInput>();
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var unit = ownUnits[i];
                    if (!IsTopStrategicUnit(unit)) continue;
                    var pos = UnitPosition(unit);
                    if (!pos.HasValue) continue;

                    inputs.Add(new ArmyAreaInput
                    {
                        UnitKey = UnitKey(unit),
                        AllianceId = allianceId,
                        UnitName = ObjectName(unit),
                        CommanderName = CommanderName(unit),
                        CurrentAreaKey = AreaKey(pos.Value),
                        Strength = ReadFloat(0f, unit, "groupstrengthactive", "groupstrengthdirect", "groupstrength", "strength"),
                        Readiness = ReadFloat(1f, unit, "readiness")
                    });
                }

                return ArmyAreaLedger.Build(inputs, planTargetAreaKey);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-area:build", "[ArmyArea] build failed: " + ex.Message);
                return null;
            }
        }

        internal static string AreaKey(Vector3 position)
        {
            return ArmyAreaClassifier.FromPosition(position.x, position.z);
        }

        internal static bool TryGetAnchor(string areaKey, out Vector3 anchor)
        {
            return Anchors.TryGetValue(areaKey ?? string.Empty, out anchor);
        }

        internal static int ApplyHistoricalAreaOrders(int aifactionIndex, int allianceId)
        {
            var ledger = StrategicCoordinator.Instance?.ArmyAreas?[allianceId];
            if (ledger == null) return 0;

            var faction = FindFaction(allianceId);
            if (faction == null)
            {
                OnceLog.Warning(
                    "army-area:orders:no-faction:" + allianceId,
                    $"[Patch:ArmyArea] historical area orders skipped: AICampaign faction not found for alliance={allianceId}");
                return 0;
            }

            var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
            if (ownUnits == null)
            {
                OnceLog.Warning(
                    "army-area:orders:no-ownunits:" + allianceId,
                    $"[Patch:ArmyArea] historical area orders skipped: ownunits unavailable for alliance={allianceId}");
                return 0;
            }

            int issued = 0;
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i];
                if (!IsTopStrategicUnit(unit) || !IsIdleForAreaOrder(unit, faction)) continue;

                var assignment = ledger.GetAssignment(UnitKey(unit));
                if (assignment == null || !assignment.OutOfArea) continue;
                if (!FormationDirectiveRuntime.ShouldAllowAreaMovement(allianceId, UnitKey(unit)))
                {
                    OnceLog.Info(
                        $"army-area:{allianceId}:{UnitKey(unit)}:directive-block",
                        $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area reason=formation-directive");
                    continue;
                }
                if (!TryGetAnchor(assignment.AssignedAreaKey, out var anchor)) continue;

                var position = UnitPosition(unit);
                if (position.HasValue)
                {
                    var fronts = StrategicCoordinator.Instance?.Fronts;
                    var front = fronts != null && allianceId >= 0 && allianceId < fronts.Length
                        ? fronts[allianceId]
                        : null;
                    string sourceSector = FrontSectorRuntime.SectorKey(position.Value);
                    string destinationSector = FrontSectorRuntime.SectorKey(anchor);
                    float strength = ReadFloat(0f, unit, "groupstrengthactive", "groupstrengthdirect", "groupstrength", "strength");
                    var budget = StrategicMovementBudget.EvaluateAreaMovement(front, sourceSector, destinationSector, strength);
                    if (budget != null && !budget.Allowed)
                    {
                        OnceLog.Info(
                            $"army-area:{allianceId}:{UnitKey(unit)}:budget-block",
                            $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area reason={budget.Reason}");
                        continue;
                    }
                }

                SetTheaterPosition(unit, anchor);
                if (MoveUnitTo(unit, anchor))
                {
                    issued++;
                    OnceLog.Info(
                        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}",
                        $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=return-area area={assignment.AssignedAreaKey} reason={assignment.Reason}");
                }
            }

            return issued;
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

        private static bool IsTopStrategicUnit(object unit)
        {
            if (unit == null) return false;
            try
            {
                int unitType = Convert.ToInt32(AccessTools.Field(unit.GetType(), "unittyp")?.GetValue(unit) ?? -1);
                bool top = Convert.ToBoolean(AccessTools.Field(unit.GetType(), "istopunit")?.GetValue(unit) ?? false);
                object garrison = AccessTools.Field(unit.GetType(), "garrisonreference")?.GetValue(unit);
                float strength = ReadFloat(0f, unit, "groupstrengthdirect");
                if (strength <= 0f) strength = ReadFloat(0f, unit, "groupstrengthactive");
                return unitType >= 14 &&
                       unitType <= 16 &&
                       top &&
                       garrison == null &&
                       strength > 1000f;
            }
            catch { return false; }
        }

        private static bool IsIdleForAreaOrder(object unit, object faction)
        {
            try
            {
                if (Convert.ToBoolean(AccessTools.Field(unit.GetType(), "inbattle")?.GetValue(unit) ?? false)) return false;
                if (Convert.ToBoolean(AccessTools.Field(unit.GetType(), "onretreat")?.GetValue(unit) ?? false)) return false;
                if (Convert.ToInt32(AccessTools.Field(unit.GetType(), "regimentpaths")?.GetValue(unit) ?? 0) > 0) return false;
                if (ContainsUnit(faction, "unitsinoffensiveoperations", unit)) return false;
                if (ContainsUnit(faction, "unitsindefensiveoperations", unit)) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool ContainsUnit(object faction, string fieldName, object unit)
        {
            var list = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
            if (list == null) return false;
            return list.Contains(unit);
        }

        private static string UnitKey(object unit)
        {
            string name = ObjectName(unit);
            int commander = -1;
            try { commander = Convert.ToInt32(AccessTools.Field(unit.GetType(), "commander")?.GetValue(unit) ?? -1); }
            catch { }
            return name + ":" + commander.ToString();
        }

        private static string ObjectName(object obj)
        {
            var unityObj = obj as UnityEngine.Object;
            return unityObj != null ? unityObj.name : obj?.ToString() ?? "<unknown>";
        }

        private static string CommanderName(object unit)
        {
            try
            {
                int commanderId = Convert.ToInt32(AccessTools.Field(unit.GetType(), "commander")?.GetValue(unit) ?? -1);
                var commanders = AccessTools.Field(AccessTools.TypeByName("GameVars"), "commander")?.GetValue(null) as IList;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return null;
                var commander = commanders[commanderId];
                return AccessTools.Field(commander.GetType(), "combinedname")?.GetValue(commander) as string;
            }
            catch { return null; }
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

        private static void SetTheaterPosition(object unit, Vector3 position)
        {
            try
            {
                AccessTools.Field(unit.GetType(), "theaterposition")?.SetValue(unit, position);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-area:theaterposition", "[Patch:ArmyArea] theaterposition write failed: " + ex.Message);
            }
        }

        private static bool MoveUnitTo(object unit, Vector3 position)
        {
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName("AICampaign"), "MoveUnitTo");
                if (method == null)
                {
                    OnceLog.Warning("army-area:move-method", "[Patch:ArmyArea] AICampaign.MoveUnitTo not found");
                    return false;
                }
                return Convert.ToBoolean(method.Invoke(null, new object[] { unit, position, true, false, true }));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-area:move", "[Patch:ArmyArea] movement order failed: " + ex.Message);
                return false;
            }
        }

        private static float ReadFloat(float fallback, object target, params string[] fields)
        {
            if (target == null) return fallback;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = AccessTools.Field(target.GetType(), fields[i]);
                if (field == null) continue;
                try { return Math.Max(0f, Convert.ToSingle(field.GetValue(target))); }
                catch { return fallback; }
            }
            return fallback;
        }
    }
}
