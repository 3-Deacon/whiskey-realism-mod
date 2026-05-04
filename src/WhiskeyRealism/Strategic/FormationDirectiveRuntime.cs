using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class FormationDirectiveRuntime
    {
        internal static FormationDirectiveLedger BuildForAlliance(int allianceId, EraStage era, string planTargetAreaKey)
        {
            try
            {
                var faction = FindFaction(allianceId);
                if (faction == null) return null;

                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                if (ownUnits == null) return null;

                bool grandArmyStructure = GrandArmyStructure(allianceId);
                var snapshots = new List<FormationSnapshot>();
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var snapshot = SnapshotUnit(allianceId, ownUnits[i], grandArmyStructure);
                    if (snapshot != null)
                    {
                        snapshot.IsPlanTargetArea = !string.IsNullOrEmpty(planTargetAreaKey) &&
                                                    string.Equals(snapshot.AreaKey, planTargetAreaKey, StringComparison.OrdinalIgnoreCase);
                        snapshots.Add(snapshot);
                    }
                }

                return FormationDirectiveLedger.Build(snapshots, era, planTargetAreaKey);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:build", "[FormationDirective] build failed: " + ex.Message);
                return null;
            }
        }

        internal static FormationSnapshot SnapshotUnit(int allianceId, object unit, bool grandArmyStructure)
        {
            try
            {
                if (unit == null) return null;
                int unitType = ReadInt(unit, -1, "unittyp");
                if (unitType < 14 || unitType > 16) return null;

                var position = UnitPosition(unit);
                var snapshot = new FormationSnapshot
                {
                    UnitKey = UnitKey(unit),
                    ParentUnitKey = ParentUnitKey(unit),
                    AllianceId = allianceId,
                    UnitName = ObjectName(unit),
                    CommanderName = CommanderName(unit),
                    UnitType = unitType,
                    IsTopUnit = ReadBool(unit, false, "istopunit"),
                    IsGarrisoned = ReadObject(unit, "garrisonreference") != null,
                    GrandArmyStructureAvailable = grandArmyStructure,
                    GroupStrengthActive = ReadFloat(unit, 0f, "groupstrengthactive", "groupstrengthdirect", "groupstrength"),
                    GroupStrengthDirect = ReadFloat(unit, 0f, "groupstrengthdirect", "groupstrengthactive", "groupstrength"),
                    Morale = FormationSnapshot.Clamp01(ReadFloat(unit, 1f, "groupmorale", "morale")),
                    Readiness = FormationSnapshot.Clamp01(ReadFloat(unit, 1f, "readiness")),
                    RifleAmmo = ReadSupplyState(unit, 0, ReadFloat(unit, 1f, "groupammo")),
                    ArtilleryAmmo = ReadSupplyState(unit, 1, ReadFloat(unit, 1f, "groupammo")),
                    Supply = ReadSupply(unit),
                    Fatigue = FormationSnapshot.Clamp01(ReadFloat(unit, 0f, "groupfatigue", "fatigue")),
                    WeaponFirepower = EstimateWeaponFirepower(unit),
                    CommandRange = ReadFloat(unit, 0f, "commanderrange"),
                    BugleRange = ReadFloat(unit, 0f, "buglerange"),
                    InBattle = ReadBool(unit, false, "inbattle"),
                    OnRetreat = ReadBool(unit, false, "onretreat"),
                    HasActivePath = ReadInt(unit, 0, "regimentpaths") > 0,
                    IsCavalryCapable = HasCavalryStrength(unit)
                };

                if (position.HasValue)
                {
                    snapshot.AreaKey = ArmyAreaRuntime.AreaKey(position.Value);
                    snapshot.SectorKey = FrontSectorRuntime.SectorKey(position.Value);
                    PopulateFrontContext(allianceId, snapshot);
                }

                PopulateLocalPressure(snapshot, unit, allianceId);
                return snapshot;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:snapshot", "[FormationDirective] snapshot failed: " + ex.Message);
                return null;
            }
        }

        internal static bool ShouldAllowAreaMovement(int allianceId, string unitKey)
        {
            try
            {
                var assignment = GetAssignment(allianceId, unitKey);
                if (assignment == null) return true;
                if (!assignment.DirectMovementAllowed) return false;
                if (assignment.Directive == FormationDirective.Recover) return false;
                if (assignment.Directive == FormationDirective.Delay) return false;
                if (assignment.Directive == FormationDirective.Concede) return false;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:area-movement", "[FormationDirective] area movement gate failed: " + ex.Message);
                return true;
            }
        }

        internal static bool AllowsArmyGroupAttachment(int allianceId, string unitKey)
        {
            try
            {
                var assignment = GetAssignment(allianceId, unitKey);
                if (assignment == null) return false;
                if (assignment.Level == FormationLevel.Corps || assignment.Level == FormationLevel.Army) return true;
                if (assignment.Level != FormationLevel.Division) return false;
                return assignment.Directive == FormationDirective.Reinforce ||
                       assignment.Directive == FormationDirective.Reserve ||
                       assignment.Directive == FormationDirective.Guard ||
                       assignment.Directive == FormationDirective.Mass;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:armygroup-attachment", "[FormationDirective] army-group attachment gate failed: " + ex.Message);
                return false;
            }
        }

        private static FormationDirectiveAssignment GetAssignment(int allianceId, string unitKey)
        {
            if (allianceId < 0 || string.IsNullOrEmpty(unitKey)) return null;
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator == null) return null;

            object ledgers = AccessTools.Field(coordinator.GetType(), "FormationDirectives")?.GetValue(coordinator);
            if (ledgers == null)
                ledgers = AccessTools.Property(coordinator.GetType(), "FormationDirectives")?.GetValue(coordinator, null);

            var array = ledgers as FormationDirectiveLedger[];
            if (array == null || allianceId >= array.Length) return null;
            return array[allianceId]?.GetAssignment(unitKey);
        }

        private static void PopulateFrontContext(int allianceId, FormationSnapshot snapshot)
        {
            try
            {
                var fronts = StrategicCoordinator.Instance?.Fronts;
                if (fronts == null || allianceId < 0 || allianceId >= fronts.Length) return;
                var sector = fronts[allianceId]?.GetSector(snapshot.SectorKey);
                if (sector == null) return;
                snapshot.FrontPosture = sector.Posture;
                snapshot.IsCriticalSector = sector.IsCritical;
                snapshot.IsPlanTargetArea = sector.IsPlanTarget;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:front-context", "[FormationDirective] front context failed: " + ex.Message);
            }
        }

        private static void PopulateLocalPressure(FormationSnapshot snapshot, object unit, int allianceId)
        {
            try
            {
                var position = UnitPosition(unit);
                if (!position.HasValue || BattleUnits.campaignunitlist == null) return;

                float supportRange = snapshot.CommandRange > 0f ? snapshot.CommandRange : snapshot.BugleRange;
                float enemyRange = snapshot.BugleRange > 0f ? snapshot.BugleRange * 2f : snapshot.CommandRange;

                for (int i = 0; i < BattleUnits.campaignunitlist.Count; i++)
                {
                    var other = BattleUnits.campaignunitlist[i];
                    if (other == null || ReferenceEquals(other, unit)) continue;
                    if (!IsTopFormation(other)) continue;

                    float distance = Vector3.Distance(position.Value, other.transform.position);
                    if (other.alliance == allianceId)
                    {
                        if (supportRange > 0f && distance <= supportRange)
                        {
                            snapshot.LocalFriendlySupportStrength += Math.Max(0f, other.groupstrengthactive);
                            snapshot.SupportCanReach = true;
                        }
                    }
                    else if (enemyRange > 0f && distance <= enemyRange)
                    {
                        snapshot.LocalEnemyStrength += Math.Max(0f, other.groupstrengthactive);
                        snapshot.LocalEnemyExchangePressure += EstimateEnemyExchangePressure(other);
                        var level = FormationSnapshot.LevelFromUnitType(other.unittyp);
                        if ((int)level > (int)snapshot.VisibleEnemyLevel)
                            snapshot.VisibleEnemyLevel = level;
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:local-pressure", "[FormationDirective] local pressure failed: " + ex.Message);
            }
        }

        private static object FindFaction(int allianceId)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var faction = list[i];
                if (ReadInt(faction, -1, "allianceid") == allianceId) return faction;
            }
            return null;
        }

        private static bool GrandArmyStructure(int allianceId)
        {
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName("AICampaign"), "GrandArmyStructure");
                if (method == null) return false;
                return Convert.ToBoolean(method.Invoke(null, new object[] { allianceId }));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:grand-army", "[FormationDirective] GrandArmyStructure failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsTopFormation(Regiment unit)
        {
            return unit != null &&
                   unit.istopunit &&
                   unit.garrisonreference == null &&
                   unit.unittyp >= 14 &&
                   unit.unittyp <= 16;
        }

        private static string ParentUnitKey(object unit)
        {
            try
            {
                if (unit is Component component && component.transform.parent != null)
                {
                    var parent = component.transform.parent.GetComponent<Regiment>();
                    if (parent != null) return UnitKey(parent);
                }

                var parentObject = ReadObject(unit, "parentregiment");
                if (parentObject is GameObject gameObject)
                {
                    var parent = gameObject.GetComponent<Regiment>();
                    if (parent != null) return UnitKey(parent);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:parent", "[FormationDirective] parent lookup failed: " + ex.Message);
            }
            return null;
        }

        private static string UnitKey(object unit)
        {
            string name = ObjectName(unit);
            int commander = ReadInt(unit, -1, "commander");
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
                int commanderId = ReadInt(unit, -1, "commander");
                var commanders = AccessTools.Field(AccessTools.TypeByName("GameVars"), "commander")?.GetValue(null) as IList;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return null;
                var commander = commanders[commanderId];
                return AccessTools.Field(commander.GetType(), "combinedname")?.GetValue(commander) as string;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:commander", "[FormationDirective] commander lookup failed: " + ex.Message);
                return null;
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

        private static float ReadSupplyState(object unit, int index, float fallback)
        {
            try
            {
                var values = ReadFloatArray(unit, "groupsupplystate");
                if (values != null && index >= 0 && index < values.Length)
                    return FormationSnapshot.Clamp01(values[index]);
            }
            catch { }
            return FormationSnapshot.Clamp01(fallback);
        }

        private static float ReadSupply(object unit)
        {
            try
            {
                var values = ReadFloatArray(unit, "groupsupplystate");
                if (values != null && values.Length > 3)
                {
                    float provisions = FormationSnapshot.Clamp01(values[2]);
                    float forage = FormationSnapshot.Clamp01(values[3]);
                    return Math.Min(provisions, forage);
                }
            }
            catch { }
            return 1f;
        }

        private static bool HasCavalryStrength(object unit)
        {
            var values = ReadIntArray(unit, "groupstrengthperunittyp");
            if (values == null) values = ReadIntArray(unit, "groupstrengthperunittypcampaigndirect");
            if (values == null) return false;

            int cavalry = values.Length > 1 ? Math.Max(0, values[1]) : 0;
            int mountedArtillery = values.Length > 4 ? Math.Max(0, values[4]) : 0;
            return cavalry + mountedArtillery > 0;
        }

        private static float EstimateWeaponFirepower(object unit)
        {
            float direct = ReadFloat(unit, 0f, "groupstrengthperunittypcampaigndirect", "groupstrengthperunittyp");
            if (direct <= 0f)
                direct = ReadFloat(unit, 0f, "groupstrengthactive", "groupstrengthdirect", "groupstrength");

            float guns = ReadFloat(unit, 0f, "groupstatsgunsactive", "groupstatsguns");
            return Math.Max(1f, direct + guns * 65f);
        }

        private static float EstimateEnemyExchangePressure(Regiment unit)
        {
            if (unit == null) return 0f;
            float strength = Math.Max(0f, unit.groupstrengthactive);
            float guns = Math.Max(0, unit.groupstatsgunsactive);
            return strength + guns * 65f;
        }

        private static object ReadObject(object target, string fieldName)
        {
            if (target == null) return null;
            try { return AccessTools.Field(target.GetType(), fieldName)?.GetValue(target); }
            catch { return null; }
        }

        private static int ReadInt(object target, int fallback, params string[] fields)
        {
            float value = ReadFloat(target, fallback, fields);
            return Convert.ToInt32(value);
        }

        private static bool ReadBool(object target, bool fallback, string fieldName)
        {
            if (target == null) return fallback;
            var field = AccessTools.Field(target.GetType(), fieldName);
            if (field == null) return fallback;
            try { return Convert.ToBoolean(field.GetValue(target)); }
            catch { return fallback; }
        }

        private static float ReadFloat(object target, float fallback, params string[] fields)
        {
            if (target == null) return fallback;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = AccessTools.Field(target.GetType(), fields[i]);
                if (field == null) continue;
                try
                {
                    var value = field.GetValue(target);
                    if (value is float[] floats) return Sum(floats);
                    if (value is int[] ints) return Sum(ints);
                    return Math.Max(0f, Convert.ToSingle(value));
                }
                catch { return fallback; }
            }
            return fallback;
        }

        private static float[] ReadFloatArray(object target, string fieldName)
        {
            return ReadObject(target, fieldName) as float[];
        }

        private static int[] ReadIntArray(object target, string fieldName)
        {
            return ReadObject(target, fieldName) as int[];
        }

        private static float Sum(float[] values)
        {
            if (values == null) return 0f;
            float total = 0f;
            for (int i = 0; i < values.Length; i++) total += Math.Max(0f, values[i]);
            return total;
        }

        private static float Sum(int[] values)
        {
            if (values == null) return 0f;
            float total = 0f;
            for (int i = 0; i < values.Length; i++) total += Math.Max(0, values[i]);
            return total;
        }
    }
}
