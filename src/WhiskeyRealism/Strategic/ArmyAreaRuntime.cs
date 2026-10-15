using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class ArmyAreaRuntime
    {
        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();

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

                var ownUnits = VanillaFieldCache.Get(faction, "ownunits") as IList;
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

            var ownUnits = VanillaFieldCache.Get(faction, "ownunits") as IList;
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
                    EmitCampaignInfoOnce(
                        $"army-area:{allianceId}:{UnitKey(unit)}:directive-block",
                        $"[ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area reason=formation-directive");
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
                        EmitCampaignInfoOnce(
                            $"army-area:{allianceId}:{UnitKey(unit)}:budget-block",
                            $"[ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area reason={budget.Reason}");
                        continue;
                    }
                }

                var regiment = unit as Regiment;
                var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
                {
                    AllianceId = allianceId,
                    AifactionIndex = aifactionIndex,
                    Unit = regiment,
                    TargetPosition = anchor,
                    TargetName = assignment.AssignedAreaKey,
                    ObjectiveId = -1,
                    Intent = WlStrategicIntent.Redeploy,
                    Width = 20f,
                    Depth = 20f,
                    SourceSystem = "ArmyArea"
                });

                if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
                {
                    issued++;
                    EmitCampaignInfoOnce(
                        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}:wl-order",
                        $"[ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=wl-current-order area={assignment.AssignedAreaKey} type={bridgeDecision.WlOrderType} reason={assignment.Reason}");
                    continue;
                }

                if (!bridgeDecision.MayDirectMove)
                {
                    EmitCampaignInfoOnce(
                        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}:wl-skip:{bridgeDecision.Result}",
                        $"[ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
                    continue;
                }

                // No theaterposition write: vanilla UpdateCampaignTheaters (17034)
                // resets/reassigns it from the unit's real position; forcing the
                // anchor distorted CheckCombinationOfUnits (17141) for the cycle.
                if (MoveUnitTo(unit, anchor))
                {
                    issued++;
                    EmitCampaignInfoOnce(
                        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}",
                        $"[ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=return-area area={assignment.AssignedAreaKey} reason={assignment.Reason}");
                }
            }

            return issued;
        }

        private const int AifactionScanLimit = 8;

        private static object FindFaction(int allianceId)
        {
            // AICampaign.aifaction holds one entry per alliance (incl. Europe = 2).
            for (int i = 0; i < AifactionScanLimit; i++)
            {
                if (AICampaignReflect.GetAllianceId(i) == allianceId)
                    return AICampaignReflect.GetFaction(i);
            }
            return null;
        }

        private static bool IsTopStrategicUnit(object unit)
        {
            if (unit == null) return false;
            try
            {
                int unitType = Convert.ToInt32(VanillaFieldCache.Get(unit, "unittyp") ?? -1);
                bool top = Convert.ToBoolean(VanillaFieldCache.Get(unit, "istopunit") ?? false);
                object garrison = VanillaFieldCache.Get(unit, "garrisonreference");
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

        // 2026-10-14 (C-ArmyArea): reuse vanilla's full busy set so area orders
        // never pull capital defenders, depot/fort builders, raid or sea-invasion
        // forces, queued defensive movers or town-takers - vanilla would send
        // them straight back (ping-pong).
        private static bool IsIdleForAreaOrder(object unit, object faction)
        {
            try
            {
                var regiment = unit as Regiment;
                if (regiment == null) return false;
                var busy = new AreaOrderBusyFlags
                {
                    InBattle = regiment.inbattle,
                    OnRetreat = regiment.onretreat,
                    HasPaths = regiment.regimentpaths > 0,
                    InOffensiveOperation = ContainsUnit(faction, "unitsinoffensiveoperations", unit),
                    InDefensiveOperation = ContainsUnit(faction, "unitsindefensiveoperations", unit),
                    DefendingCapital = ContainsUnit(faction, "groupstodefendcapital", unit),
                    BuildingSupplyDepot = ContainsUnit(faction, "unitsconstructingsupplydepots", unit),
                    BuildingFort = AICampaign.FortConstructionOrder.UnitAlreadyConstructing(regiment),
                    RaidForce = AICampaign.RaidForce.IsRaidUnit(regiment),
                    SeaInvasionForce = AICampaign.SeaInvasionForce.GetSeaInvasionForceReference(regiment) != null,
                    DefensiveOrderQueued = AICampaign.DefensiveMovingOrder.OrderRunning(regiment),
                    TakingTown = IsUnitTakingTown(regiment)
                };
                return CampaignMilitaryGates.IsIdleForAreaOrder(busy);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("army-area:idle-check", "[Patch:ArmyArea] idle check failed: " + ex.Message);
                return false;
            }
        }

        private static bool _isUnitTakingTownResolved;
        private static System.Reflection.MethodInfo _isUnitTakingTown;

        // Private static AICampaign.IsUnitTakingTown(Regiment) (decompile 13438).
        // Unresolvable -> treat as busy (fail closed).
        private static bool IsUnitTakingTown(Regiment unit)
        {
            if (!_isUnitTakingTownResolved)
            {
                _isUnitTakingTownResolved = true;
                _isUnitTakingTown = AccessTools.Method(typeof(AICampaign), "IsUnitTakingTown", new[] { typeof(Regiment) });
                if (_isUnitTakingTown == null)
                    OnceLog.Warning("army-area:taking-town", "[Patch:ArmyArea] AICampaign.IsUnitTakingTown(Regiment) not found; area orders treat units as busy");
            }
            if (_isUnitTakingTown == null) return true;
            return Convert.ToBoolean(_isUnitTakingTown.Invoke(null, new object[] { unit }));
        }

        private static bool ContainsUnit(object faction, string fieldName, object unit)
        {
            var list = VanillaFieldCache.Get(faction, fieldName) as IList;
            if (list == null) return false;
            return list.Contains(unit);
        }

        private static string UnitKey(object unit)
        {
            string name = ObjectName(unit);
            int commander = -1;
            try { commander = Convert.ToInt32(VanillaFieldCache.Get(unit, "commander") ?? -1); }
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
                int commanderId = Convert.ToInt32(VanillaFieldCache.Get(unit, "commander") ?? -1);
                var commanders = GameVars.commander;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return null;
                return commanders[commanderId]?.combinedname;
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

        private static bool MoveUnitTo(object unit, Vector3 position)
        {
            try
            {
                var regiment = unit as Regiment;
                if (regiment == null) return false;
                // AICampaign.MoveUnitTo(Regiment, Vector3, movearoundenemyunits, useseamovementonly, checkforreadiness) (14479)
                return AICampaign.MoveUnitTo(regiment, position, movearoundenemyunits: true, useseamovementonly: false, checkforreadiness: true);
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
                var field = VanillaFieldCache.Field(target.GetType(), fields[i]);
                if (field == null) continue;
                try { return Math.Max(0f, Convert.ToSingle(field.GetValue(target))); }
                catch { return fallback; }
            }
            return fallback;
        }

        private static void EmitCampaignInfoOnce(string key, string line)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }
    }
}
